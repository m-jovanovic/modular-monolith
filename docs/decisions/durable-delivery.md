# Acknowledge durable receipt and apply locally

**Accepted.** A process can stop between handing an event to PickupDesk and recording the relay acknowledgement. The delivery attempt and the business fact have distinct identities. Every retry retains the original message ID.

The subscriber commits a receipt unique on subscriber and message ID. The same payload can be acknowledged again. Reusing the ID for a different fact is an error. Applying the projection and completing the receipt share the consumer transaction. The hold sequence protects the result when different facts arrive out of order. Keeping terminal state allows a late placement to be recognized after a pickup disappears from the active queue.

A reservation response can precede its appearance at the desk. Staff need a lag signal and an operator needs a way to inspect and re-drive quarantined receipts. Replay depends on retained information. Restore a lost database from a consistent backup or snapshot plus subsequent history. [Receiver](../../src/Modules/PickupDesk/PickupDesk/Inbox/PickupInbox.cs) · [Projection](../../src/Modules/PickupDesk/PickupDesk/Inbox/ApplyHoldSnapshot.cs) · [Recovery tests](../../tests/Modules/PickupDesk/PickupDeskTests.cs)

## PostgreSQL transport comparison

In the [isolated Wolverine 6.65.1 experiment](../../tests/Messaging/TransportEvaluation/README.md), a native Npgsql transaction rolled back both the hold and outgoing envelope. Killing a publisher after commit left one of each. The actual PickupDesk listener could not read the Lending-owned transport queue under the reference's module-only runtime grants. Granting access to the two transport tables let a restarted sender deliver the same hold ID while Lending's business table stayed private.

Keep the explicit [relay](../../src/Modules/Lending/Delivery/HoldRelay.cs) and subscriber-owned receipt in this reference. The relay acknowledges after the receiver's receipt has committed. Its public message ID and subscriber ID survive a retry, and the receiver applies the snapshot and marks the receipt complete in a local transaction. This approach keeps queue storage and operator recovery in the owning module at the cost of maintaining the relay, inbox worker and their interruption tests. A system with a shared transport schema can choose the tested library integration and make its grants explicit.
