from __future__ import annotations

from dataclasses import asdict, dataclass
from typing import Dict, List
import random

import numpy as np

from .preferences import (
    AudioMood,
    BrightnessPreference,
    DissonanceTolerance,
    Instrument,
    Level3,
    MixPreference,
    PREFERENCE_VALUE_MAP,
    ReverbPreference,
    RhythmPreference,
    SoundStyle,
    TempoPreference,
    UserPreferenceProfile,
)


CONTROL_DIMENSIONS: List[str] = [
    "intensity",
    "density",
    "brightness",
    "tempo",
    "fade",
    "music_mix",
    "ambient_mix",
    "rhythm_amount",
    "nature_level",
    "reverb_amount",
    "volume_level",
    "novelty_amount",
    "dissonance_allowance",
]


@dataclass(frozen=True)
class SimulatedUser:
    preferences: UserPreferenceProfile
    target_profile: Dict[str, float]
    tolerance_widths: Dict[str, float]
    relaxation_responsiveness: float
    confidence_sensitivity: float

    def to_dict(self) -> Dict[str, object]:
        return {
            "preferences": asdict(self.preferences),
            "target_profile": self.target_profile,
            "tolerance_widths": self.tolerance_widths,
            "relaxation_responsiveness": self.relaxation_responsiveness,
            "confidence_sensitivity": self.confidence_sensitivity,
        }


class SimulatedUserGenerator:
    def __init__(self, seed: int = 0) -> None:
        self.rng = random.Random(seed)
        self.np_rng = np.random.default_rng(seed)

    def generate_users(self, count: int, prefix: str) -> List[SimulatedUser]:
        return [self.generate_user(f"{prefix}_{index:03d}") for index in range(count)]

    def generate_user(self, user_id: str) -> SimulatedUser:
        preferences = UserPreferenceProfile(
            user_id=user_id,
            sound_style=self.rng.choice(list(SoundStyle)),
            preferred_instruments=self._sample_instruments(),
            preferred_tempo=self.rng.choice(list(TempoPreference)),
            preferred_mood=self.rng.choice(list(AudioMood)),
            preferred_audio_intensity=self.rng.choice(list(Level3)),
            ambient_music_balance=self.rng.choice(list(MixPreference)),
            brightness_preference=self.rng.choice(list(BrightnessPreference)),
            novelty_tolerance=self.rng.choice(list(Level3)),
            dissonance_tolerance=self.rng.choice(list(DissonanceTolerance)),
            rhythm_preference=self.rng.choice(list(RhythmPreference)),
            nature_sound_preference=self.rng.choice(list(Level3)),
            reverb_preference=self.rng.choice(list(ReverbPreference)),
            volume_preference=self.rng.choice(list(Level3)),
        )

        target = PreferenceMapper.map_preferences_to_targets(preferences)
        tolerances = {
            dimension: float(self.np_rng.uniform(0.08, 0.22))
            for dimension in CONTROL_DIMENSIONS
        }

        if preferences.novelty_tolerance == Level3.LOW:
            tolerances["novelty_amount"] *= 0.7
        elif preferences.novelty_tolerance == Level3.HIGH:
            tolerances["novelty_amount"] *= 1.3

        return SimulatedUser(
            preferences=preferences,
            target_profile=target,
            tolerance_widths=tolerances,
            relaxation_responsiveness=float(self.np_rng.uniform(0.7, 1.2)),
            confidence_sensitivity=float(self.np_rng.uniform(0.8, 1.3)),
        )

    def _sample_instruments(self) -> List[Instrument]:
        instrument_count = self.rng.randint(1, 3)
        return self.rng.sample(list(Instrument), instrument_count)


class PreferenceMapper:
    @staticmethod
    def map_preferences_to_targets(preferences: UserPreferenceProfile) -> Dict[str, float]:
        tempo_value = PREFERENCE_VALUE_MAP[preferences.preferred_tempo]
        intensity_value = PREFERENCE_VALUE_MAP[preferences.preferred_audio_intensity]
        brightness_value = PREFERENCE_VALUE_MAP[preferences.brightness_preference]
        novelty_value = PREFERENCE_VALUE_MAP[preferences.novelty_tolerance]
        rhythm_value = PREFERENCE_VALUE_MAP[preferences.rhythm_preference]
        reverb_value = PREFERENCE_VALUE_MAP[preferences.reverb_preference]
        volume_value = PREFERENCE_VALUE_MAP[preferences.volume_preference]
        nature_value = PREFERENCE_VALUE_MAP[preferences.nature_sound_preference]
        ambient_target = PREFERENCE_VALUE_MAP[preferences.ambient_music_balance]
        music_target = 1.0 - ambient_target

        sound_style_adjustments = {
            SoundStyle.FOREST: {"nature_level": 0.9, "brightness": 0.4, "reverb_amount": 0.55},
            SoundStyle.OCEAN: {"nature_level": 0.75, "brightness": 0.55, "reverb_amount": 0.70},
            SoundStyle.RAIN: {"nature_level": 0.80, "brightness": 0.30, "reverb_amount": 0.60},
            SoundStyle.TEMPLE: {"nature_level": 0.20, "brightness": 0.45, "reverb_amount": 0.80},
            SoundStyle.STUDIO: {"nature_level": 0.05, "brightness": 0.60, "reverb_amount": 0.35},
        }

        mood_adjustments = {
            AudioMood.CALM: {"fade": 0.70, "intensity": -0.10},
            AudioMood.FOCUSED: {"fade": 0.45, "brightness": 0.08},
            AudioMood.SLEEPY: {"fade": 0.82, "tempo": -0.10},
            AudioMood.ENERGIZED: {"fade": 0.30, "intensity": 0.12, "tempo": 0.10},
        }

        density = np.clip(0.25 + (tempo_value * 0.25) + (rhythm_value * 0.20), 0.0, 1.0)
        fade = 0.55
        dissonance = 0.15 if preferences.dissonance_tolerance == DissonanceTolerance.AVOID_DISSONANCE else 0.45

        target = {
            "intensity": intensity_value,
            "density": float(density),
            "brightness": brightness_value,
            "tempo": tempo_value,
            "fade": fade,
            "music_mix": float(music_target),
            "ambient_mix": float(ambient_target),
            "rhythm_amount": rhythm_value,
            "nature_level": nature_value,
            "reverb_amount": reverb_value,
            "volume_level": volume_value,
            "novelty_amount": novelty_value,
            "dissonance_allowance": dissonance,
        }

        for key, value in sound_style_adjustments[preferences.sound_style].items():
            target[key] = float(np.clip(value, 0.0, 1.0))

        mood_adjustment = mood_adjustments[preferences.preferred_mood]
        for key, adjustment in mood_adjustment.items():
            target[key] = float(np.clip(target.get(key, 0.5) + adjustment if isinstance(adjustment, float) else adjustment, 0.0, 1.0))

        if Instrument.FLUTE in preferences.preferred_instruments:
            target["brightness"] = float(np.clip(target["brightness"] + 0.06, 0.0, 1.0))
        if Instrument.PAD in preferences.preferred_instruments or Instrument.DRONE in preferences.preferred_instruments:
            target["fade"] = float(np.clip(target["fade"] + 0.08, 0.0, 1.0))
        if Instrument.STRINGS in preferences.preferred_instruments:
            target["density"] = float(np.clip(target["density"] + 0.05, 0.0, 1.0))
        if Instrument.BELLS in preferences.preferred_instruments:
            target["brightness"] = float(np.clip(target["brightness"] + 0.04, 0.0, 1.0))

        mix_total = max(1e-6, target["music_mix"] + target["ambient_mix"])
        target["music_mix"] /= mix_total
        target["ambient_mix"] /= mix_total
        return target
