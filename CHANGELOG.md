# Changelog

All notable changes to this project will be documented in this file.

## [V10.0.0] - 10-30-2026

### Added
- `OpenEdgeLargeObjectOperationException`: queries that compare, sort, `IN`-filter or `LIKE`-match a `clob` or `blob` column now fail before they are sent. OpenEdge rejects these operations, and with a parameter they can crash the OpenEdge SQL server

### Changed
- Updated to .NET 10 and EF Core 10
- `Contains` on a collection now generates `IN (?, ?, ?)` with parameters instead of inlined constants, following the EF Core 10 default

### Fixed
- Fixed scaffolding failing with a service registration error
- Fixed `Count()` failing with an invalid cast, because OpenEdge returns `COUNT` as a 64-bit value
- Fixed `Any()` and `All()` generating invalid SQL
- Fixed queries failing when a parameter is used more than once
- Fixed `DateTimeOffset` parameters failing for `datetime-tz` and `timestamp_timezone` columns
- Added the missing mapping for `fixchar` columns

## [V9.0.7] - 09-19-2025

### Fixed
- Fixed inline fetch/offset statements being cached incorrectly

### Added
- Extended support for datetimezone type

## [V9.0.6] - 08-21-2025

### Fixed
- Fixed issue with boolean comparison not being translated correctly in some cases

### Added
- Added support for DateOnly type in queries

## [V9.0.4] - 08-07-2025

### Fixed

- Fixed issue with OFFSET/FETCH parameters not being inlined correctly in complex queries
- Added support for nested queries with Skip/Take

### Changed

- Commented out functionality for casting COUNT in generated queries to INT