# OmegaDrive name migration

## Public name

The public product name from v0.0.12 is:

**OmegaDrive Driver Manager**

Short UI references use **OmegaDrive**.

## Compatibility identifiers kept temporarily

The following identifiers intentionally keep the historical name during the
first migration stage:

- GitHub repository name;
- C# namespaces;
- Windows Service internal name;
- named-pipe name;
- existing Program Files and ProgramData directories;
- current-user startup value name;
- uninstall registry key path.

Changing all of those at once would turn a visual rebrand into a destructive
installation migration. They can be renamed later with explicit migration code
and runtime validation.

## Setup transition

v0.0.12 publishes both `OmegaDrive-Setup.exe` and a byte-identical legacy
setup alias. Older clients know only the legacy filename; the new client
prefers the OmegaDrive filename.

## Naming note

The name OmegaDrive is used by unrelated products and projects in other
markets. Before a broad public/commercial launch, perform a dedicated legal
trademark and domain review. The staged technical migration makes it possible
to change the public brand again without first renaming low-level compatibility
identifiers.
