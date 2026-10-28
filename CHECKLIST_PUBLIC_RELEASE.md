# Public release checklist

Use this checklist before publishing a release.

## Legal and project basics
- [ ] repository license is present and clearly identified
- [ ] CODE_OF_CONDUCT.md is published
- [ ] CONTRIBUTING.md is published
- [ ] SECURITY.md is published
- [ ] privacy policy is defined if the app handles personal data

## Documentation
- [ ] README is up to date
- [ ] release instructions are documented
- [ ] roadmap reflects current product priorities
- [ ] changelog is maintained with conventional commit history
- [ ] badges and version references are accurate

## Release integrity
- [ ] version is defined from a single source of truth
- [ ] Git tag format matches the release version strategy
- [ ] release artifacts are versioned consistently
- [ ] checksum or signature validation is performed before update installation
- [ ] rollback procedure is documented and tested

## Security review
- [ ] dependency review has been performed
- [ ] known security issues are evaluated
- [ ] signed or validated release assets are used
- [ ] privileged installation paths are minimized
- [ ] access and recovery flows are documented

## CI and testing
- [ ] build validation passes
- [ ] test suite passes
- [ ] release workflow validation passes
- [ ] update validation checks run before publishing
- [ ] install path compatibility is verified

## Distribution
- [ ] release notes are completed
- [ ] assets have expected names and sizes
- [ ] public download links are valid
- [ ] stable URL alias points to the correct release
- [ ] users can recover from a failed update

## Final sign-off
- [ ] maintainers reviewed the release
- [ ] security review was completed for release-sensitive changes
- [ ] public issue templates and support paths are ready
- [ ] release is marked as ready after final validation
