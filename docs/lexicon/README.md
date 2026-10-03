# Lexicon

These terms describe Enoch's publication model.

| Term           | Meaning                                                                                                                                                       |
| -------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Run            | A durable publication record of agent work, with a request, published material, lifecycle state, and optional terminal outcome                                |
| Publisher      | A client authorized by the Enoch bearer token to create or mutate active runs                                                                                 |
| Reader         | A human or client reading published records through the deployment's read-authentication boundary                                                             |
| Request        | The original input captured when a run starts                                                                                                                 |
| Plan revision  | A published version of the intended work                                                                                                                      |
| Semantic event | A named progress observation, rather than a raw transcript message                                                                                            |
| Evidence       | Supporting text content with metadata and an integrity hash                                                                                                   |
| Artifact       | Published file content with metadata and an integrity hash                                                                                                    |
| Result         | The published answer or output summary, separate from the terminal outcome                                                                                    |
| State          | The run lifecycle value: queued, running, waiting, or finished; current creation starts in running                                                            |
| Outcome        | The terminal classification: success, partial, failed, cancelled, or expired                                                                                  |
| Sequence       | The manifest counter used by plan and event publications; consistency and retry findings are tracked in CURRENT                                               |
| Bundle         | The filesystem directory containing a run's canonical records and published content; the current HTTP bundle route returns a document, not a complete archive |
| Exploitation   | Deployment and operation of the delivered application, including persistence and recovery                                                                     |

See [CURRENT](../CURRENT.md) for implementation limitations and [ADRs](../adr/README.md) for accepted decisions.
