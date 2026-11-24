# Scoping a feature

Use the smallest complete user-visible change as the unit of review.

1. Name the actor, business action and owning module.
2. Record the base commit, allowed paths, existing public contract and example slice.
3. Give a normal input and its expected result. Include one denied, duplicated, concurrent or out-of-order input relevant to the change.
4. Name the test that exercises the outcome. Keep the grading expectation independent of the proposed solution.
5. Hand off the commit, observed results, unresolved questions and the next reader of the change.

For a staff queue filter, PickupDesk owns the read model and staff authorization. A minimal change uses its existing columns, combines with other filters, preserves paging and tests a borrower request.
