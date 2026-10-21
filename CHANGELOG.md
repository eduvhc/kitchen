# Changelog

## [0.4.0](https://github.com/eduvhc/kitchen/compare/v0.3.0...v0.4.0) (2026-10-21)


### Features

* add ValidatingKit (+ .AspNetCore) and field-level errors on ResultingKit's Error ([#45](https://github.com/eduvhc/kitchen/issues/45)) ([fdf1ea5](https://github.com/eduvhc/kitchen/commit/fdf1ea5a789c0c51eb90de247998c5c9674005ea))
* LockingKit for distributed locks on Azure blob leases and PostgreSQL ([#50](https://github.com/eduvhc/kitchen/issues/50)) ([659fa2c](https://github.com/eduvhc/kitchen/commit/659fa2c89d5e281ec22081a3984902554d7a5b6a))
* MediatingKit, a source-generated mediator ([#51](https://github.com/eduvhc/kitchen/issues/51)) ([3ddc5ef](https://github.com/eduvhc/kitchen/commit/3ddc5ef2f0fe3ac386ecf08450af8e55f064dd2c))
* typed JSON documents for StoringKit ([#49](https://github.com/eduvhc/kitchen/issues/49)) ([9e4c510](https://github.com/eduvhc/kitchen/commit/9e4c510deedbae34d7b3a2a707249138cf835eae))

## [0.3.0](https://github.com/eduvhc/kitchen/compare/v0.2.4...v0.3.0) (2026-10-20)


### ⚠ BREAKING CHANGES

* organise MailingKit by vertical slice ([#44](https://github.com/eduvhc/kitchen/issues/44))
* organise MessagingKit and ResultingKit by vertical slice ([#43](https://github.com/eduvhc/kitchen/issues/43))
* custom IMessageTransport and IEmailSender implementations must return ResultingKit results instead of TransportResult/SendResult.

### refactor

* organise MailingKit by vertical slice ([#44](https://github.com/eduvhc/kitchen/issues/44)) ([55a8d91](https://github.com/eduvhc/kitchen/commit/55a8d918969c194e03e5d6bd11e9425e33f138e4))
* organise MessagingKit and ResultingKit by vertical slice ([#43](https://github.com/eduvhc/kitchen/issues/43)) ([c59640e](https://github.com/eduvhc/kitchen/commit/c59640e47022404323cbdb0180dd4c8e66e69e21))


### Features

* add MailingKit.Resend email sender ([#31](https://github.com/eduvhc/kitchen/issues/31)) ([7a00cce](https://github.com/eduvhc/kitchen/commit/7a00ccee420a403886e9a1e6e5ec5291b50d40c5))
* add ResultingKit and use it for transport results ([#29](https://github.com/eduvhc/kitchen/issues/29)) ([8402d58](https://github.com/eduvhc/kitchen/commit/8402d58597297a0bfe5edd73609e0accd567893d))
* add ResultingKit.AspNetCore for mapping results to HTTP responses ([#33](https://github.com/eduvhc/kitchen/issues/33)) ([3a0fd6b](https://github.com/eduvhc/kitchen/commit/3a0fd6b56dd3bba6eafda11d7c0acdc306c3871e))
* add StoringKit for object storage on Azure Blob Storage and S3-compatible stores ([#40](https://github.com/eduvhc/kitchen/issues/40)) ([39fe7de](https://github.com/eduvhc/kitchen/commit/39fe7de6b62d3b7ebbe69731f74da457066d6831))
* instrument MailingKit email sends with traces and metrics ([#36](https://github.com/eduvhc/kitchen/issues/36)) ([28e45ce](https://github.com/eduvhc/kitchen/commit/28e45ce03127d8bcdde36d471ca9f51bb86468ff))
* instrument MessagingKit with OpenTelemetry-conventional traces and metrics ([#32](https://github.com/eduvhc/kitchen/issues/32)) ([fc02246](https://github.com/eduvhc/kitchen/commit/fc02246892e275deea88e88fe30bdebd6bf3d8bf))
* make retried email sends idempotent ([#35](https://github.com/eduvhc/kitchen/issues/35)) ([af4588f](https://github.com/eduvhc/kitchen/commit/af4588fcd56d01a977ed62f7193cb93c6871a519))


### Bug Fixes

* resend the first attempt's content so a retry never changes the payload ([#39](https://github.com/eduvhc/kitchen/issues/39)) ([05e8f56](https://github.com/eduvhc/kitchen/commit/05e8f56025f85d144a2dce573dac9c8daad6eab4))

## [0.2.4](https://github.com/eduvhc/kitchen/compare/v0.2.3...v0.2.4) (2026-09-14)


### Dependencies

* bump MinVer to 8.0.0 ([3f057a1](https://github.com/eduvhc/kitchen/commit/3f057a14e8b69d294499d198d886054e3396edc2))
* bump MSTest to 4.4.0 ([3f057a1](https://github.com/eduvhc/kitchen/commit/3f057a14e8b69d294499d198d886054e3396edc2))
* bump Testcontainers to 4.15.0 (Testcontainers, Azurite, MsSql, PostgreSql, RabbitMq) ([3f057a1](https://github.com/eduvhc/kitchen/commit/3f057a14e8b69d294499d198d886054e3396edc2))


### Chores

* release 0.2.4 ([3f057a1](https://github.com/eduvhc/kitchen/commit/3f057a14e8b69d294499d198d886054e3396edc2))

## [0.2.3](https://github.com/eduvhc/kitchen/compare/v0.2.2...v0.2.3) (2026-09-14)


### Features

* **testing-kit:** expose message headers from RabbitMqFixture (ConsumeMessageAsync, PublishAsync with headers) ([47cd618](https://github.com/eduvhc/kitchen/commit/47cd618a2326f3dbe160285db59d0d17c46fd110))


### Bug Fixes

* **testing-kit:** purging a queue that does not exist yet is a no-op ([47cd618](https://github.com/eduvhc/kitchen/commit/47cd618a2326f3dbe160285db59d0d17c46fd110))


### Dependencies

* bump Testcontainers to 4.14.0, clearing the NU1903 SSH.NET advisory without suppressions ([47cd618](https://github.com/eduvhc/kitchen/commit/47cd618a2326f3dbe160285db59d0d17c46fd110))

## [0.2.2](https://github.com/eduvhc/kitchen/compare/v0.2.1...v0.2.2) (2026-07-31)


### Bug Fixes

* anchor releases to the v-prefixed tag ([cf0c117](https://github.com/eduvhc/kitchen/commit/cf0c11767fd053e48f959921862750956a34c19c))
* ship the family as one version ([68940b1](https://github.com/eduvhc/kitchen/commit/68940b1a9218481311abc1d8367dcf73bd3f8dc2))
