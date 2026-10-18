"""Offline protocol tests. No Google client is constructed and no music is generated."""
import asyncio
import base64
import json
import unittest
import os
from contextlib import asynccontextmanager
from types import SimpleNamespace
from unittest.mock import patch
from fastapi import WebSocketDisconnect

from services.lyria_backend import app as bridge


class FakeSocket:
    def __init__(self):
        self.messages = []
        self.commands = asyncio.Queue()
        self.closed = False

    async def accept(self):
        pass

    async def close(self, code=1000):
        self.closed = True

    async def receive_text(self):
        item = await self.commands.get()
        if item is None:
            raise WebSocketDisconnect()
        return json.dumps(item)

    async def send_text(self, text):
        self.messages.append(json.loads(text))


class FakeSession:
    def __init__(self):
        self.calls = []
        self.chunks = asyncio.Queue()

    async def set_music_generation_config(self, config):
        self.calls.append(("config", config))

    async def set_weighted_prompts(self, prompts):
        self.calls.append(("prompts", prompts))

    async def reset_context(self):
        self.calls.append(("reset", None))

    async def play(self):
        self.calls.append(("play", None))

    async def receive(self):
        while True:
            message = await self.chunks.get()
            if message is None:
                return
            yield message


def audio(data=b"\x01\x00\x02\x00"):
    return SimpleNamespace(server_content=SimpleNamespace(audio_chunks=[SimpleNamespace(data=data)]))


class RealtimeProtocolTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.types = patch.object(bridge, "genai_types", SimpleNamespace(
            LiveMusicGenerationConfig=lambda **kw: kw, WeightedPrompt=lambda **kw: kw,
            Scale=SimpleNamespace(C_MAJOR_A_MINOR="C_MAJOR_A_MINOR", D_MAJOR_B_MINOR="D_MAJOR_B_MINOR"),
            MusicGenerationMode=SimpleNamespace()))
        self.types.start()
        self.addCleanup(self.types.stop)
        self.session, self.socket, self.state = FakeSession(), FakeSocket(), bridge.LiveMusicStreamState()

    async def command(self, request="a" * 32, bpm=70, density=0.3, scale="C_MAJOR_A_MINOR", **extra):
        await bridge._handle_live_music_client_message(self.session, self.socket, {
            "type": "config", "requestId": request,
            "config": {"bpm": bpm, "density": density, "scale": scale}, **extra}, self.state)

    async def test_density_keeps_context_and_acknowledges_request(self):
        await self.command()
        await self.command(request="b" * 32, density=0.33)
        self.assertEqual([name for name, _ in self.session.calls], ["config", "config"])
        self.assertEqual(self.socket.messages[-1]["requestId"], "b" * 32)
        self.assertEqual(self.socket.messages[-1]["state"], "config_updated")
        self.assertEqual(self.session.calls[-1][1]["density"], 0.33)

    async def test_bpm_and_scale_reset_after_config_once_each(self):
        await self.command()
        await self.command(bpm=72)
        await self.command(bpm=72)
        await self.command(bpm=72, scale="D_MAJOR_B_MINOR")
        self.assertEqual([name for name, _ in self.session.calls],
                         ["config", "config", "reset", "config", "config", "reset"])

    async def test_audio_after_ack_has_revision_and_original_pcm(self):
        await self.command()
        await self.session.chunks.put(audio())
        await self.session.chunks.put(None)
        await bridge._forward_live_music_messages(self.session, self.socket, self.state)
        ack, chunk = self.socket.messages
        self.assertEqual((ack["type"], chunk["type"]), ("state", "audio"))
        self.assertEqual(chunk["requestId"], ack["requestId"])
        self.assertEqual(base64.b64decode(chunk["data"]), b"\x01\x00\x02\x00")
        self.assertEqual((chunk["sampleRate"], chunk["channels"], chunk["format"]), (48000, 2, "pcm_s16le"))

    async def test_command_ack_precedes_concurrent_audio(self):
        applying, finish = asyncio.Event(), asyncio.Event()
        original = self.session.set_music_generation_config

        async def slow_config(config):
            applying.set()
            await finish.wait()
            await original(config)

        self.session.set_music_generation_config = slow_config
        command = asyncio.create_task(self.command())
        await asyncio.wait_for(applying.wait(), 2)
        # This chunk was received during the old revision and must not be relabelled.
        forward = asyncio.create_task(bridge._forward_live_music_messages(self.session, self.socket, self.state))
        await self.session.chunks.put(audio())
        await asyncio.sleep(0)
        self.assertEqual(self.socket.messages, [])
        finish.set()
        await asyncio.wait_for(command, 2)
        await asyncio.sleep(0)
        await self.session.chunks.put(audio(b"\x03\x00"))
        await self.session.chunks.put(None)
        await asyncio.wait_for(forward, 2)
        self.assertEqual([message["type"] for message in self.socket.messages], ["state", "audio"])
        self.assertEqual(base64.b64decode(self.socket.messages[1]["data"]), b"\x03\x00")

    async def test_config_failure_never_acknowledges_or_advances_revision(self):
        async def fail(config):
            raise RuntimeError("Test provider rejection")
        self.session.set_music_generation_config = fail
        with self.assertRaises(RuntimeError):
            await self.command()
        self.assertEqual(self.state.request_id, "")
        self.assertEqual(self.socket.messages, [])

    async def test_invalid_request_id_rejected_before_mutation(self):
        with self.assertRaises(ValueError):
            await self.command(request="participant/private/path")
        self.assertEqual(self.session.calls, [])

    async def test_initial_sync_stays_compatible_without_request_id(self):
        await bridge._handle_live_music_client_message(self.session, self.socket, {
            "type": "sync", "weightedPrompts": [{"text": "calm instrumental meditation", "weight": 1}],
            "config": {"bpm": 70, "density": 0.3}, "autoPlay": True}, self.state)
        self.assertEqual([name for name, _ in self.session.calls], ["prompts", "config", "play"])
        self.assertEqual(self.socket.messages[-1]["state"], "playing")
        self.assertEqual(self.socket.messages[-1]["requestId"], "")

    async def run_route(self, failure=False):
        closed = asyncio.Event()
        session = self.session

        @asynccontextmanager
        async def connect(**kwargs):
            yield session

        async def close():
            closed.set()

        client = SimpleNamespace(aio=SimpleNamespace(live=SimpleNamespace(music=SimpleNamespace(connect=connect)), aclose=close))
        if failure:
            async def receive():
                raise RuntimeError("fixture-secret upstream failed")
                yield
            session.receive = receive
        with patch.object(bridge, "genai", SimpleNamespace(Client=lambda **kwargs: client)), \
                patch.dict(os.environ, {"GEMINI_API_KEY": "fixture-secret"}):
            await asyncio.wait_for(bridge.live_music(self.socket), 2)
        self.assertTrue(closed.is_set())
        self.assertTrue(self.socket.closed)

    async def test_provider_end_closes_bridge_instead_of_hanging_on_commands(self):
        await self.session.chunks.put(None)
        await self.run_route()
        self.assertEqual(self.socket.messages[0]["actionPlaybackProtocol"], bridge.ACTION_PLAYBACK_PROTOCOL)

    async def test_provider_failure_is_forwarded_without_leaking_credentials(self):
        await self.run_route(failure=True)
        self.assertEqual(self.socket.messages[-1]["type"], "error")
        self.assertNotIn("fixture-secret", self.socket.messages[-1]["message"])

    async def test_client_disconnect_releases_provider_session(self):
        await self.socket.commands.put(None)
        await self.run_route()

    async def test_route_forwards_audio_until_client_finishes(self):
        await self.session.chunks.put(audio())
        await self.session.chunks.put(None)
        await self.run_route()
        self.assertTrue(any(m["type"] == "audio" for m in self.socket.messages))


if __name__ == "__main__":
    unittest.main()
