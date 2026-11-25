# UI-only changes

This update is limited to presentation and layout. It does not add features or intentionally change SSH, SFTP, ADB, router, or script execution logic.

- Replaces the squeezed TabControl layout with a two-column navigation/content template so the selected page can use the full remaining window width.
- Changes output panels to a consistent light palette with readable dark text.
- Splits the crowded connection bar into profile actions and startup/behaviour preferences.
- Makes the Scripts browser/output split resizable and gives the output pane the remaining width.
- Keeps the Console output in a full-width, star-sized content row.
- Keeps the existing control names and event handlers so existing functionality remains wired.

The Windows WPF build and live router/ADB interactions still need to be run on Windows after upload.
