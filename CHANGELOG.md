# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-10-18

### Added
- Added support for the Motospeed X6 wireless gaming mouse via direct HID polling (`MotospeedManager`).
- Added `[Motospeed]` configuration section in `appsettings.toml` with `enabled` toggle and customizable `pollPeriod`.
- Added Motospeed device manager service registration and life cycle handling.
- Added standalone (.NET 8 bundled) and framework-dependent release builds.
