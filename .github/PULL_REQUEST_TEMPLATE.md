## Problem
<!-- The problem this pull request solves. Link the issue if there is one. -->

## Approach
<!-- Summarize the implementation and the important design decisions. -->

## Automated validation
<!-- List the exact commands you ran and the CI checks that passed. -->

- [ ] API tests pass: `docker build --target backend-test .`
- [ ] Interface tests pass: `(cd frontend && npm test)`
- [ ] Browser journey passes: `docker build -t partscout:test . && tools/ci-e2e.sh partscout:test`
- [ ] Pull-request CI passed on the latest commit
- [ ] No API key, password, `.env` or personal data in the change

## Manual testing
<!-- Describe what you tried by hand: workflows, error states, both languages,
     data persistence across restarts, upgrade from the previous version, logs. -->

- [ ] New interface text is in both languages (`frontend/src/i18n.ts`)
- [ ] For a supplier change: tested against the real API, not only demo mode
- [ ] For a data or settings change: an existing `/data` folder still works after the upgrade
- [ ] Screenshots are attached for visible changes

## Review
- [ ] Review comments are answered or resolved

## Limitations and follow-up
<!-- Skipped validation, known limitations, follow-up work. Write "None" if there are none. -->

## Terms
By opening this pull request, I agree that my contribution is distributed under the project's terms (see LICENSE), including the non-commercial restriction.
