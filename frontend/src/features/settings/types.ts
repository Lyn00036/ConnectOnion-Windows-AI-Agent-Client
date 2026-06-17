/** Type definitions for future desktop settings payloads. */

// TODO(settings-model): Expand this into the persisted desktop settings schema.
// Likely fields:
// - theme and display density
// - default agent id
// - request timeout and retry preferences
// - streaming/progress display preferences
// - local history retention policy
// Keep credentials out of this type; use a secure credential store instead.

export type SettingsState = {
  theme: 'system' | 'light' | 'dark'
}
