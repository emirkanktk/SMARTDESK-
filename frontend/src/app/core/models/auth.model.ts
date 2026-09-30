export interface LoginRequest {
  email: string;
  password: string;
}

/** Body of `POST /api/auth/reset-password`; email and token come from the emailed link. */
export interface ResetPasswordRequest {
  email: string;
  token: string;
  newPassword: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  /** The password was set by someone else and must be changed first (ADR-0021). */
  passwordChangeRequired?: boolean;
}

/** Body of `POST /api/auth/change-password`; the answer is a new token. */
export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

/** Body of `PUT /api/auth/me`: the signed-in user's own name. */
export interface UpdateProfileRequest {
  firstName: string;
  lastName: string;
}

export interface CurrentUser {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  roles: string[];
}
