# ADR-007: Provisional local telemetry logging

## Status

Accepted as a provisional pilot configuration. This approval permits runtime
logging for the time-constrained MVP; the event schema remains explicitly
pre-freeze until Step 14.

## Context

Research-relevant adaptive behavior must be reconstructable from local logs
when networking is unavailable. The existing telemetry pipeline writes one
UTF-8 JSON object per line into a session-specific file and verifies that every
event matches the configured schema and logging configuration identifiers.

The local sink flushes immediately for critical events. Noncritical events can
be flushed in small batches to reduce filesystem work on Quest 2.

## Provisional pilot values

| Setting | Candidate | Meaning |
|---|---|---|
| Configuration ID | `adaptive-vr-telemetry-pilot-v1` | Identifies this pilot logging behavior in every event |
| Configuration version | `1` | First provisional pilot configuration |
| Event schema ID | `adaptive-vr-telemetry` | Stable event-family identifier |
| Event schema version | `0.1-pilot` | Explicitly pre-freeze schema version for Step 13 validation |
| Flush every | 8 ordinary events | Limits routine filesystem flushes; critical events still flush immediately |

## Decision

Version the profile as `adaptive-vr-telemetry-pilot-v1` and enable its runtime
approval gate using the values above. Logs remain local JSON Lines under
Unity's persistent-data path, separated by session. Participant identity must
remain pseudonymous.

## Failure and recovery behavior

- Critical events force a flush independent of the batch count.
- A crash can leave at most the current batch of noncritical events unflushed.
- Transport loss does not disable local logging.
- Telemetry configuration or schema mismatches fail explicitly rather than
  mixing incompatible events in one file.

## Limitations

- `0.1-pilot` is not the final frozen study schema.
- Retention, deletion, export, and sensitive-data handling still require the
  research protocol outside this Unity configuration.
- Step 13 must inspect a complete JSONL session before the schema is frozen in
  Step 14.

## Validation plan

- Run telemetry configuration, formatter, recorder, and local-sink EditMode
  tests.
- Verify critical events flush immediately and ordinary events flush at eight.
- Run a complete session and confirm decisions, rewards, phase changes,
  rejected inputs, safety outcomes, and completion can be reconstructed.
- Confirm only pseudonymous participant identifiers appear in the log.
