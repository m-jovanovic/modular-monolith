# Grade a change from outside its checkout

**Accepted.** Contributors can give a coding agent the same module guide, public contracts and test commands that a human would use. A plausible patch still needs independent evidence.

Freeze the starting commit, request and permitted paths. Run the candidate in a fresh public checkout. Apply its patch to an isolated copy of that base and inject behavioral checks from the grading checkout. Verify changed paths and module references before compiling and testing. Keep one deliberately invalid solution so changes to the grader cannot silently make it accept a known boundary violation.

Record the tool, attempt count, commands, diff and human review separately from the deterministic test result. An observed outcome describes that task and input. Re-run the exercise after changing its contract or prompt.
