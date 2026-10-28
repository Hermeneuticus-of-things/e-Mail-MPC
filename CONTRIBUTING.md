# Contributing

Thanks for helping improve the project.

## How to contribute

- open an issue before starting large or architectural changes
- keep changes focused and easy to review
- prefer small, well-scoped pull requests
- document behavior changes and migration impacts
- validate correctness before submitting

## Commit style

This project intends to use Conventional Commits.

Examples:

- `feat: add encrypted local storage format v1`
- `fix: handle update validation failure path`
- `docs: expand release installation instructions`
- `security: harden update asset verification`

## Pull request expectations

- include a concise summary
- describe the problem being solved
- explain any user-facing impact
- call out documentation or migration changes
- ensure the relevant checks pass before requesting review

## Code quality

- prefer clear, maintainable code patterns
- avoid broad refactors unrelated to the task
- include tests when behavior changes are significant
- preserve compatibility where practical

## Release-sensitive work

Because this project includes secure installation and release integrity, changes touching:

- install logic
- update logic
- signing or checksum verification
- storage format versioning
- privacy-sensitive flows

should be reviewed extremely carefully.
