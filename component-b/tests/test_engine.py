from server import engine


def test_missing_model_runtime_is_reported_without_crashing(monkeypatch):
    def missing_runtime():
        raise ModuleNotFoundError("No module named 'tensorflow'")

    monkeypatch.setattr(engine, "_artifacts", None)
    monkeypatch.setattr(engine, "_reason", None)
    monkeypatch.setattr(engine, "load_model", missing_runtime)

    assert engine.new_stream() is None
    assert "tensorflow" in engine.unavailable_reason()
