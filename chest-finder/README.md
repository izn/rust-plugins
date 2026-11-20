# ChestFinder

Rust plugin that searches nearby containers the player is authorized to access.

## Install

Place `ChestFinder.cs` in `carbon/plugins/`.

## Usage

```text
/finditem <item>
```

Permission:

```text
chestfinder.use
```

Grant it with the command system of the installed framework:

```text
c.grant group default chestfinder.use
```

## Configuration

The plugin creates its configuration after the first load. It controls search
distance, cooldown, marker limits, marker appearance, and text colors.

## Compatibility

Carbon only.

The Workbench Recycle Bin is not supported yet.
