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
}

export interface CurrentUser {
  id: string;
  firstName: string;
  lastName: string;
  email: string;
  roles: string[];
}
