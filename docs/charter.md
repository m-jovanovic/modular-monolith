# Equipment library charter

Revision 1 · 24 November 2026 · Maintainer: Milan Jovanović

A community equipment library lends individual, barcoded copies of a catalog item. A member wants to borrow a cordless drill, and a librarian wants to know which copy to set aside. The reference follows one question through the system: when two members reserve the last available copy, who gets it and what does the librarian see?

| Module | Business responsibility | Interaction |
| --- | --- | --- |
| Catalog | Own the identity and description of a type of equipment. | Lending looks up an item through Catalog's public contract when a librarian enrolls a physical copy. |
| Lending | Own each physical copy and its holds. Decide reservation, cancellation and collection. | Commit an accepted transition and its outgoing event in one local transaction. |
| PickupDesk | Maintain a staff view of copies awaiting pickup. | Consume Lending's events idempotently. Read the queue from the module's own data. |

The first release targets one ASP.NET Core process and one PostgreSQL instance. Each module owns its data. Only Lending decides the winning hold. The pickup queue can lag during delivery and must converge when processing resumes. Those statements are acceptance criteria for the implementation.

We use feature-oriented application code, explicit public contracts, architecture tests and executable examples to make changes reviewable by people and coding agents. Each architectural decision should identify a business consequence and a test that can demonstrate it.

This project uses original examples. Milan's Evently course informs the coding conventions. Kamil Grzybek's project influenced the use of a glossary, architecture decisions, diagrams and tests to explain a modular system. [Modular Monolith with DDD](https://github.com/kgrzybek/modular-monolith-with-ddd)
