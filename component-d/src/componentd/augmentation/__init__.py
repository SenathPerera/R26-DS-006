"""Domain augmentation: degrade clean studio audio to resemble phone/real
recordings, closing the acoustic gap. See README.md for panel-study notes."""

from .augment import (
    add_background_noise,
    apply_reverb,
    augment,
    codec_roundtrip,
    telephone_bandpass,
)

__all__ = [
    "augment", "add_background_noise", "apply_reverb",
    "codec_roundtrip", "telephone_bandpass",
]
