# Password Manager — REVIEW_CHECKLIST

## 1. Purpose

Use this checklist to review any code or report produced for the Password Manager project.

The priority order is:

1. Security
2. Correctness
3. Maintainability
4. UX speed
5. Visual polish

Visual polish never overrides security.

---

## 2. Implementation Compliance Review

Check whether the implementation stayed inside the documented project direction.

- [ ] Did it follow the current ROADMAP and PROGRESS direction?
- [ ] Did it avoid undocumented product decisions?
- [ ] Did it avoid undocumented architecture changes?
- [ ] Did it avoid changing vault format?
- [ ] Did it avoid adding out-of-scope features?
- [ ] Did it report failures honestly?
- [ ] Did it clearly separate facts from recommendations?
- [ ] Did it avoid hiding license or security risks?

Reject or revise if implementation silently changes security or product direction.

## 2.1 Workflow Efficiency Review

- [ ] Was the work kept in the simplest useful form?
- [ ] Did small fixes avoid unnecessary process?
- [ ] Were manual test instructions concise?
- [ ] If the task was large/risky, was second-eye review considered?
- [ ] Did the output stay focused on implementation instead of broad advice?
- [ ] Did the output avoid unnecessary documentation bloat?

---

## 3. Vault Security Review

- [ ] Master password is never stored.
- [ ] Master password is never logged.
- [ ] Entry passwords are never logged.
- [ ] Plaintext credentials are never written to disk.
- [ ] No plaintext JSON/TXT/XML credential files are created.
- [ ] No secrets are placed in sample source files.
- [ ] No secrets are placed in comments.
- [ ] No secrets are placed in appsettings.
- [ ] No debug output reveals secrets.
- [ ] Wrong master password fails safely.
- [ ] Vault corruption/integrity failure is handled.
- [ ] The app does not claim unsupported security features.
- [ ] No custom crypto is introduced without approval.

---

## 4. Architecture Review

- [ ] UI layer does not depend on KDBX library-specific types.
- [ ] Core layer does not depend on KDBX library-specific types.
- [ ] KDBX-specific code is isolated in Infrastructure.
- [ ] Domain models are clean.
- [ ] Interfaces are stable enough for later UI work.
- [ ] App can swap vault implementation without rewriting UI.
- [ ] File storage concerns are separated from domain logic.
- [ ] Backup/sync placeholders do not pollute vault core.
- [ ] No god classes.
- [ ] No everything-in-MainPage.xaml.cs implementation.

---

## 5. KDBX Library Review

- [ ] Package name is documented.
- [ ] Package version is documented.
- [ ] License is documented.
- [ ] License risk is explained.
- [ ] Target frameworks are documented.
- [ ] Windows compatibility is tested.
- [ ] Android compatibility is tested or plausibly assessed.
- [ ] Read support works.
- [ ] Write support works.
- [ ] Save/reopen works.
- [ ] Wrong password behavior is tested.
- [ ] Maintenance risk is documented.
- [ ] API limitations are documented.

---

## 6. Prototype Behavior Review

The task must prove:

- [ ] New vault can be created.
- [ ] Test entry can be added.
- [ ] Vault can be saved.
- [ ] Vault can be reopened.
- [ ] Test entry exists after reopen.
- [ ] Wrong password fails.
- [ ] App does not create plaintext side files.
- [ ] App does not log secrets.
- [ ] Implementation can be run by the user.
- [ ] Run steps are clear.

---

## 7. Code Quality Review

- [ ] Code is readable.
- [ ] Names are clear.
- [ ] Async code is not abused.
- [ ] Exceptions are handled deliberately.
- [ ] Security-critical errors are not swallowed.
- [ ] No unnecessary abstractions.
- [ ] No over-engineered factory jungle.
- [ ] No magic strings for sensitive behavior unless centralized.
- [ ] No temporary hacks left unexplained.
- [ ] No TODOs that hide security flaws.
- [ ] Tests are meaningful.

---

## 8. UX Safety Review

Even in early task, check:

- [ ] Password fields are intended to be hidden by default.
- [ ] Copy behavior is planned to clear clipboard.
- [ ] Reveal behavior is planned to be temporary.
- [ ] Dangerous actions are planned to require confirmation.
- [ ] Lock behavior is planned.
- [ ] Error messages do not reveal sensitive data.

---

## 9. Out-of-Scope Review

Reject unnecessary work if it added:

- [ ] Browser extension
- [ ] Autofill
- [ ] Native cloud API integration
- [ ] Team/family sharing
- [ ] Web app
- [ ] iOS implementation
- [ ] Full premium UI
- [ ] Long cinematic animations
- [ ] Custom recovery system
- [ ] Custom crypto
- [ ] Plaintext import/export system

---

## 10. Decision Result

After review, choose one:

## Proceed

Use if:

- Task works.
- Security rules are respected.
- Architecture is clean.
- License is acceptable.
- Windows path works.
- Android path is plausible.

## Request Revision

Use if:

- Task mostly works but has fixable issues.
- Architecture needs cleanup.
- Tests are missing.
- Report is incomplete.
- Some risks need clarification.

## Investigate Further

Use if:

- Library works partially.
- License is unclear.
- Android support is uncertain.
- API is risky but not clearly unusable.

## Reject

Use if:

- Security rule is violated.
- License is incompatible.
- Secrets leak.
- Custom crypto was added.
- Architecture is messy.
- KDBX read/write does not work.
- Android path is blocked.
- Implementation relies on unsafe hacks.
