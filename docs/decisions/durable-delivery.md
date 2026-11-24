# Acknowledge durable receipt and apply locally

**Accepted.** A process can stop between handing an event to PickupDesk and recording the relay acknowledgement. The delivery attempt and the business fact have distinct identities. Every retry retains the original message ID.

The subscriber commits a receipt unique on subscriber and message ID. The same payload can be acknowledged again. Reusing the ID for a different fact is an error. Applying the projection and completing the receipt share the consumer transaction. The hold sequence protects the result when different facts arrive out of order. Keeping terminal state allows a late placement to be recognized after a pickup disappears from the active queue.

A reservation response can precede its appearance at the desk. Staff need a lag signal and an operator needs a way to inspect and re-drive quarantined receipts. Replay depends on retained information. Restore a lost database from a consistent backup or snapshot plus subsequent history. [Receiver](../../src/Modules/PickupDesk/PickupDesk/Inbox/PickupInbox.cs) · [Projection](../../src/Modules/PickupDesk/PickupDesk/Inbox/ApplyHoldSnapshot.cs) · [Recovery tests](../../tests/Modules/PickupDesk/PickupDeskTests.cs)
