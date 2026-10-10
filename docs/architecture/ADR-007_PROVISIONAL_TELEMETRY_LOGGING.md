# ADR-007: Provisional local telemetry logging

## Status

Superseded for the active pilot by ADR-017. This ADR records the
`temple-pond-pilot-freeze-v1` telemetry baseline and remains the history of
schema `1.0-pilot`; it does not describe the active telemetry profile.

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
| Configuration ID | `adaptive-vr-telemetry-pilot-v2` | Identifies the frozen pilot logging behavior in every event |
| Configuration version | `2` | Adds reconstructable profile and preference-initialization metadata |
| Event schema ID | `adaptive-vr-telemetry` | Stable event-family identifier |
| Event schema version | `1.0-pilot` | Frozen engineering-pilot event schema |
| Flush every | 8 ordinary events | Limits routine filesystem flushes; critical events still flush immediately |

## Decision

Version the profile as `adaptive-vr-telemetry-pilot-v2` and enable its runtime
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

- `1.0-pilot` is frozen for the engineering pilot, not the final main-study
  schema.
- Retention, deletion, export, and sensitive-data handling still require the
  research protocol outside this Unity configuration.
- A complete JSONL session must be inspected before promoting the schema to the
  main study.

## Validation plan

- Run telemetry configuration, formatter, recorder, and local-sink EditMode
  tests.
- Verify critical events flush immediately and ordinary events flush at eight.
- Run a complete session and confirm decisions, rewards, phase changes,
  rejected inputs, safety outcomes, and completion can be reconstructed.
- Confirm only pseudonymous participant identifiers appear in the log.
