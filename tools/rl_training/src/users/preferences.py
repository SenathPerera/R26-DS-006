from __future__ import annotations

from dataclasses import dataclass
from enum import Enum
from typing import Dict, List


class SoundStyle(str, Enum):
    FOREST = "forest_ambient"
    OCEAN = "ocean_ambient"
    RAIN = "rain_ambient"
    TEMPLE = "temple_ambient"
    STUDIO = "soft_studio_ambient"


class Instrument(str, Enum):
    PIANO = "piano"
    FLUTE = "flute"
    PAD = "pad"
    STRINGS = "strings"
    BELLS = "bells"
    DRONE = "drone"


class TempoPreference(str, Enum):
    SLOW = "slow"
    MEDIUM = "medium"
    FAST = "fast"


class AudioMood(str, Enum):
    CALM = "calm"
    FOCUSED = "focused"
    SLEEPY = "sleepy"
    ENERGIZED = "energized"


class Level3(str, Enum):
    LOW = "low"
    MEDIUM = "medium"
    HIGH = "high"


class MixPreference(str, Enum):
    MOSTLY_AMBIENCE = "mostly_ambience"
    BALANCED = "balanced"
    MOSTLY_MUSIC = "mostly_music"


class BrightnessPreference(str, Enum):
    SOFT_DARK = "soft_dark"
    NEUTRAL = "neutral"
    BRIGHT_CLEAR = "bright_clear"


class DissonanceTolerance(str, Enum):
    AVOID_DISSONANCE = "avoid_dissonance"
    MILD_TENSION_ALLOWED = "mild_tension_allowed"


class RhythmPreference(str, Enum):
    MINIMAL = "minimal_rhythm"
    GENTLE = "gentle_pulse"
    MORE_MOTION = "more_motion"


class ReverbPreference(str, Enum):
    DRY = "dry"
    BALANCED = "balanced"
    SPACIOUS = "spacious"


PREFERENCE_VALUE_MAP: Dict[Enum, float] = {
    TempoPreference.SLOW: 0.2,
    TempoPreference.MEDIUM: 0.5,
    TempoPreference.FAST: 0.8,
    Level3.LOW: 0.2,
    Level3.MEDIUM: 0.5,
    Level3.HIGH: 0.8,
    MixPreference.MOSTLY_AMBIENCE: 0.8,
    MixPreference.BALANCED: 0.5,
    MixPreference.MOSTLY_MUSIC: 0.2,
    BrightnessPreference.SOFT_DARK: 0.2,
    BrightnessPreference.NEUTRAL: 0.5,
    BrightnessPreference.BRIGHT_CLEAR: 0.8,
    RhythmPreference.MINIMAL: 0.2,
    RhythmPreference.GENTLE: 0.5,
    RhythmPreference.MORE_MOTION: 0.8,
    ReverbPreference.DRY: 0.2,
    ReverbPreference.BALANCED: 0.5,
    ReverbPreference.SPACIOUS: 0.8,
}


@dataclass(frozen=True)
class UserPreferenceProfile:
    user_id: str
    sound_style: SoundStyle
    preferred_instruments: List[Instrument]
    preferred_tempo: TempoPreference
    preferred_mood: AudioMood
    preferred_audio_intensity: Level3
    ambient_music_balance: MixPreference
    brightness_preference: BrightnessPreference
    novelty_tolerance: Level3
    dissonance_tolerance: DissonanceTolerance
    rhythm_preference: RhythmPreference
    nature_sound_preference: Level3
    reverb_preference: ReverbPreference
    volume_preference: Level3
