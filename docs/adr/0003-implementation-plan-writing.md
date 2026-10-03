# ADR-0003 — Writing bounded implementation plans

Status: accepted by user instruction, 2026-10-03.

## Decision

Write a plan as an executable specification for one bounded capability or safety invariant. It must give an implementer enough information to make the change and a reviewer enough information to decide whether it is complete. Scale its length to the work.

Read the relevant ADRs, [current-state evidence](../CURRENT.md), and actual source before fixing scope or API shapes. Reconcile stale issues with those findings. State conflicts explicitly rather than silently expanding the architecture.

Use `docs/plans/` and the filename rule in [ADR-0002](0002-plan-filenames.md). Link the parent delivery plan, where one exists, and relevant ADRs. Keep one active delivery sequence; bounded slice plans elaborate that sequence rather than creating competing roadmaps.

## Required content

Use these sections as a starting structure. Combine sections when that makes a small plan clearer, but retain their information.

| Section                       | What it must establish                                                                                                                                       |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Status and authority          | Whether the document is proposed, accepted, implemented, or superseded; parent plan, governing ADRs, and linked evidence where available                     |
| Smallest useful outcome       | The coherent application behavior enabled or safety invariant protected; observable completion and an insufficient implementation that would fail acceptance |
| Starting source and ownership | Existing capabilities and missing behavior, inspected revision, relevant source/tests, proposed homes, assignment owners, and integration owner              |
| Fixture and inputs            | Minimum authored data, controlled context, and reused assets; fixture conveniences distinguished from production rules                                       |
| Contracts and decisions       | Inputs, outputs, semantics, ownership, invalid/failure behavior, state publication, determinism, and compatibility promises relevant to the slice            |
| Implementation checkpoints    | Small complete tasks, stable task identifiers, dependencies, shared prerequisites, integration order, and the observable result of each checkpoint           |
| Acceptance criteria           | Numbered, independently assessable statements of final behavior, including failure cases and affected real consumers                                         |
| Verification and hand-back    | Focused commands, meaningful observables, broader review scope, evidence destination, and reporting requirements                                             |
| Non-goals and stop conditions | Adjacent capabilities deferred, abstractions excluded, and findings that require reporting before proceeding                                                 |

An owner can be a role until assignment. One implementer is appropriate when contracts and consumers are tightly connected. For delegated work, make file ownership disjoint, commit shared prerequisites first, and order integrations by real dependencies. The plan must not imply delegates or reviews have already run.

Each task names its owner, prerequisites, affected files or components, observable result, acceptance criteria, verification, and hand-back. A standalone task specification follows the same bounded format. Record actual execution in [TASK_LOGS](../TASK_LOGS.md) and link it from the plan.

## Make semantics explicit before implementation

Define contracts where ambiguity could change behavior. For publication work, identify required inputs, lifecycle transitions, retry identity, sequence ownership, visibility of persisted state, and interruption recovery where relevant. For browser or client work, identify the actual API envelope, navigation behavior, error behavior, and consumer of each value.

Identify the real consumer and the observable result of each new value or abstraction. Prefer existing assets and the smallest representation that serves the slice. A conceptual responsibility does not require a new class or framework.

Separate three kinds of statement:

- **Required contract:** an accepted semantic obligation, with its authority identified.
- **Proposed implementation:** a suggested file, type, shape, or fixture choice that source inspection may refine.
- **Settled implementation choice:** a concrete policy selected for this slice, with its rationale and status recorded.

A fixture size is not a universal production limit, and a selected implementation strategy is not a universal architectural formula. Settle choices that dependent consumers need before assigning those consumers. Record later choices visibly instead of presenting them as original requirements.

Inspect actual saved-data and wire contracts and demonstrated callers. State which must remain compatible and which behavior may intentionally change. Do not add duplicate storage paths, compatibility overloads, or blanket historical-output equality for hypothetical consumers.

## Acceptance proves a capability

Use numbered criteria so the hand-back can map each requirement to evidence or an explicit limitation. State inputs or trigger, observable result, and relevant invariant. Prefer public responses, persisted records after recovery, rendered user-visible behavior, and accepted state transitions over private fields, logs, or class-name assertions.

Include controlled context changes, invalid input, rejected work, interrupted operations, and actual consumers where applicable. Bound work with explicit limits and observable outcomes rather than timing assertions. Verify determinism and exact reuse where promised without requiring every intentional behavior change to reproduce historical output.

Constructing a DTO does not establish a usable API; adding a component does not establish readable published evidence. Every slice distinguishes plumbing from delivered behavior.

Keep fixtures minimal and data-driven. Renamed IDs and different valid inputs must still work when their labels and positions have no semantic meaning. Do not author expected data merely to force the selected answer unless those values are themselves the contract under test.

## Verification and evidence

Use commands supported by the repository and verify recipe and test names before presenting them as available. Label proposed commands or tests clearly. Use Docker with the declared .NET 8 and Node.js 22 runtimes for reproducible application verification. Choose focused checks at completed behavior checkpoints and relevant broader checks for combined review. Documentation-only changes need content, links, and whitespace review, not application suites.

For an actual bug, establish and retain a deterministic failing regression before the production fix. A new capability need not be recast as a historical defect. Distinguish fixture mistakes, environment failures, established software failures, and successful verification in the evidence.

The hand-back names the implementation commit when committed, changed files, intentional behavior changes, exact commands/results, acceptance mapping, findings, limitations, and deferred work. Record actual evidence in a dated [task log entry](../TASK_LOGS.md) and update [CURRENT](../CURRENT.md) when implementation facts change. The plan links that evidence and updates its status; it does not accumulate a second execution log.

Keep target architecture, implemented behavior, executed verification, and live operational verification separate. A committed plan does not establish implementation. Passing tests do not establish interruption recovery, successful backup restoration, or production usability. Claim an independent review or live check only when it actually occurred.

## Rationale and consequences

A bounded outcome, explicit contracts, and reviewable evidence let implementation and review reach the same conclusion about completion. Future plans follow that structure at the size their work requires without turning every checkpoint into a permanent milestone gate.
