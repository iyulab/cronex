# Changelog

All notable changes to Cronex are documented here.

Versions are `0.x` and breaking changes may occur in a minor bump. Dependency floors are treated as
part of the public surface: raising one can break a consumer's restore, so a raise is called out
here even when no code changed.

## 0.3.3

### Fixed

- `Cronex.Net.Hosting` declared `Microsoft.Extensions.Hosting.Abstractions` with the floating range
  `10.*`, so the floor written into the published nuspec was whatever version happened to be latest
  when the package was packed. The floor therefore moved between releases with no corresponding
  change in the source tree, and a consumer pinned below the new floor hit `NU1109` on restore with
  nothing in the diff or the release notes to explain it.

  The reference is now an exact lowest supported version, `10.0.0`. This **lowers** the floor
  relative to 0.3.2 (see below), so any consumer that could restore 0.3.2 can also restore 0.3.3.

  The test project references `Microsoft.Extensions.Hosting` at the same `10.0.0`, so the declared
  floor is exercised by the test run rather than only asserted in package metadata. A future need
  for a newer API now fails the build instead of silently moving the floor for consumers.

- Pinned the remaining floating ranges in the test and benchmark projects to the versions they were
  already resolving to. `Deterministic` builds were being restored against ranges that change
  underfoot.

### Added

- This changelog. Its absence is why the 0.3.2 floor move had nowhere to be recorded.

## 0.3.2

### Added

- The README is packed inside both packages, so the gallery page is no longer blank.

### Changed

- **Unintended, recorded retroactively:** the `Microsoft.Extensions.Hosting.Abstractions` floor of
  `Cronex.Net.Hosting` moved from `10.0.8` to `10.0.10`. This was a side effect of the floating
  `10.*` reference resolving at pack time, not a deliberate raise, and it was announced as having no
  behavioural change. Consumers pinned below `10.0.10` cannot restore 0.3.2 and should use 0.3.3,
  whose floor is `10.0.0`.

## 0.3.1

- Packages published under the `Cronex.Net` / `Cronex.Net.Hosting` ids.
