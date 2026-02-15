import axios from "axios";
import type { RegisterDto } from "../../Models/Dtos/RegisterDto";
import type { LoginResponse } from "../../Models/LoginResponse";
import type { ResetPasswordDto } from "../../Models/Dtos/ResetPasswordDto";
import type { ForgotPasswordDto } from "../../Models/Dtos/ForgotPasswordDto";
import type { LoginDto } from "../../Models/Dtos/LoginDto";
import type { ValidateEmailDto } from "../../Models/Dtos/ValidateEmailDto";
import { API_ROUTES, ROUTES } from "../../Routes/RoutesConsts";
import { useNavigate } from "react-router-dom";

export async function Login(loginDto: LoginDto): Promise<boolean> {
  try {
    const response = await axios.post<LoginResponse>(`${API_ROUTES.base}${API_ROUTES.auth.login}`, loginDto);
    localStorage.setItem("access_token", response.data.access_token);
    return true;
  } catch (error) {
    console.error("Authentication failed:", error);
    return false;
  }
}

export async function ValidateEmail(validateEmailDto: ValidateEmailDto): Promise<boolean> {
  try {
    await axios.post<LoginResponse>(`${API_ROUTES.base}${API_ROUTES.auth.validateEmail}`, validateEmailDto);
    return true;
  } catch (error) {
    return false;
  }
}

export async function Register(registerDto: RegisterDto): Promise<string[] | null> {
  try {
    registerDto.FrontendBaseUrl = `${window.location.origin}/auth/${ROUTES.auth.emailConfirmation}`;
    await axios.post(`${API_ROUTES.base}${API_ROUTES.auth.register}`, registerDto);
    return null;
  } catch (error: any) {
    if (axios.isAxiosError(error) && error.response) {
      const data = error.response.data;
      if (Array.isArray(data.Errors)) {
        return data.Errors;
      }
      if (Array.isArray(data.errors)) {
        return data.errors;
      }
      if (typeof data === "string") {
        return [data];
      }
    }
    return ["Registration failed. Please try again."];
  }
}

export async function ResetPassword(resetPasswordDto: ResetPasswordDto): Promise<string[] | null> {
  try {
    await axios.post(`${API_ROUTES.base}${API_ROUTES.auth.resetPassword}`, resetPasswordDto);
    return null;
  } catch (error: any) {
    if (axios.isAxiosError(error) && error.response) {
      const data = error.response.data;
      if (Array.isArray(data.Errors)) {
        return data.Errors;
      }
      if (Array.isArray(data.errors)) {
        return data.errors;
      }
      if (typeof data === "string") {
        return [data];
      }
    }
    return ["Password reset failed. Please try again."];
  }
}

export async function ForgotPassword(forgotPasswordDto: ForgotPasswordDto): Promise<string[] | null> {
  forgotPasswordDto.FrontendBaseUrl = `${window.location.origin}${ROUTES.auth.resetPassword}`;
  try {
    await axios.post(`${API_ROUTES.base}${API_ROUTES.auth.forgotPassword}`, forgotPasswordDto);
    return null;
  } catch (error: any) {
    if (axios.isAxiosError(error) && error.response) {
      const data = error.response.data;
      if (Array.isArray(data.Errors)) {
        return data.Errors;
      }
      if (Array.isArray(data.errors)) {
        return data.errors;
      }
      if (typeof data === "string") {
        return [data];
      }
    }
    return ["Sending reset email failed. Please try again."];
  }
}

export const isAuthenticated = (): boolean => {
  const token = localStorage.getItem("access_token");
  return !!token;
};

export const logout = (): void => {
  const navigate = useNavigate();
  localStorage.removeItem("access_token");
  navigate(ROUTES.auth.login);
};
