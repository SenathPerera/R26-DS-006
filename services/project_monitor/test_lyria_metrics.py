"""Run with the Lyria environment. Never contacts Google or generates audio."""
import unittest
from unittest.mock import patch

from fastapi import HTTPException
from services.lyria_backend import app as lyria
from .instrumentation import ServiceMetrics


class LyriaMetricsTests(unittest.TestCase):
    def setUp(self):
        self.metrics_patch = patch.object(lyria, "monitor_metrics", ServiceMetrics("lyria"))
        self.metrics_patch.start()
        self.addCleanup(self.metrics_patch.stop)

    def test_success_and_http_200_failure_are_distinguished(self):
        request = lyria.GenerateClipRequest(prompt="instrumental meditation test")
        for success in (True, False):
            result = lyria.GenerateClipResponse(success=success, requestId="test", model="test",
                                               promptUsed="test", generatedAtUtc="test")
            with patch.object(lyria, "_generate_clip", return_value=result):
                self.assertIs(result, lyria.generate_clip(request))
        op = lyria.monitor_metrics.snapshot()["operations"]["generateClip"]
        self.assertEqual(1, op["successes"])
        self.assertEqual(1, op["failures"])
        self.assertEqual(0, op["inflight"])

    def test_exception_clears_inflight_without_capturing_secret_error(self):
        request = lyria.GenerateClipRequest(prompt="instrumental meditation test")
        with patch.object(lyria, "_generate_clip", side_effect=HTTPException(500, "secret-test-detail")):
            with self.assertRaises(HTTPException):
                lyria.generate_clip(request)
        snapshot = lyria.monitor_metrics.snapshot()
        self.assertEqual(0, snapshot["operations"]["generateClip"]["inflight"])
        self.assertNotIn("secret-test-detail", str(snapshot))
