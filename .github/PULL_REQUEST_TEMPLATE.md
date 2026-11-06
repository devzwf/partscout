## What and why
<!-- What this changes, and the problem it solves. Link the issue if there is one. -->

## Checklist
- [ ] API tests pass: `docker build --target backend-test .`
- [ ] Interface tests pass: `(cd frontend && npm test)`
- [ ] Browser journey passes: `docker build -t partscout:test . && tools/ci-e2e.sh partscout:test`
- [ ] New interface text is in both languages (`frontend/src/i18n.ts`)
- [ ] No API key, password, `.env` or personal data in the change
- [ ] For a supplier change: tested against the real API, not only demo mode

## Terms
By opening this pull request, I agree that my contribution is distributed under the project's terms (see LICENSE), including the non-commercial restriction.
