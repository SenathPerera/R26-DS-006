"""Streaming output must match the batch pipeline exactly.

This is the most important test in the project. If it fails, live
predictions differ from the validated notebook results and every
reported number becomes unverifiable in deployment.
"""

import pytest


@pytest.mark.skip(reason="enable once the model is exported")
def test_streaming_matches_batch():
    # 1. take a WESAD session
    # 2. run the batch pipeline -> predictions_batch
    # 3. replay the same beats through StreamingInference
    #    -> predictions_stream
    # 4. assert they agree
    raise NotImplementedError
