/** GET /api/push: whether the server sends notifications, and the key to subscribe with. */
export interface PushStatus {
  enabled: boolean;
  publicKey: string | null;
}

/** POST /api/push/test: how many of the user's devices it went to. */
export interface PushTestResult {
  devices: number;
}
