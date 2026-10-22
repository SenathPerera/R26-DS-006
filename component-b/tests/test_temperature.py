from server.temperature import (
    SYNTHETIC_MAX_C,
    SYNTHETIC_MIN_C,
    TemperatureResolver,
)


def test_backend_generates_smooth_bounded_temperature_when_sensor_is_absent():
    resolver = TemperatureResolver(synthetic_enabled=True)

    readings = [resolver.resolve(None, 1_787_282_838.4 + offset)
                for offset in range(0, 600, 15)]

    assert all(item.source == "synthetic_backend" for item in readings)
    assert all(SYNTHETIC_MIN_C <= item.value_c <= SYNTHETIC_MAX_C
               for item in readings)
    assert max(abs(b.value_c - a.value_c)
               for a, b in zip(readings, readings[1:])) < 0.1


def test_real_temperature_is_preferred_then_briefly_cached():
    resolver = TemperatureResolver(synthetic_enabled=True)

    measured = resolver.resolve(34.125, 100.0)
    cached = resolver.resolve(None, 145.0)
    fallback = resolver.resolve(None, 161.0)

    assert (measured.value_c, measured.source) == (34.125, "wearable")
    assert (cached.value_c, cached.source) == (34.125, "wearable_cached")
    assert fallback.source == "synthetic_backend"


def test_temperature_can_be_strictly_disabled():
    reading = TemperatureResolver(synthetic_enabled=False).resolve(None, 100.0)

    assert reading.value_c is None
    assert reading.source == "unavailable"
