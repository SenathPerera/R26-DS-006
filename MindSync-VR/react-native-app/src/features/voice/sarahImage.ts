// Sarah's portrait. Drop a square PNG/JPG of the AI companion at
// src/features/voice/sarah.png and switch the line below to:
//   export const SARAH_IMAGE = require('./sarah.png');
// Every stage uses <SarahAvatar>, so she then appears across the whole flow.
// While this is null, the avatar renders the aurora orb instead.
export const SARAH_IMAGE: number | null = require('./sarah.png');
