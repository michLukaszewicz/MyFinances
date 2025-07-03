import axios from "axios";
import type { RegisterDto } from "../../Models/Dtos/RegisterDto";
import type { LoginResponse } from "../../Models/LoginResponse";
import type { ResetPasswordDto } from "../../Models/Dtos/ResetPasswordDto";
import type { ForgotPasswordDto } from "../../Models/Dtos/ForgotPasswordDto";
import type { LoginDto } from "../../Models/Dtos/LoginDto";
import type { ValidateEmailDto } from "../../Models/Dtos/ValidateEmailDto";

export async function Login(loginDto: LoginDto): Promise<boolean> {
  try {
    const response = await axios.post<LoginResponse>(`https://localhost:7121/Authentication/Login`, loginDto);
    localStorage.setItem("access_token", response.data.access_token);
    return true;
  } catch (error) {
    console.error("Authentication failed:", error);
    return false;
  }
}

export async function ValidateEmail(validateEmailDto: ValidateEmailDto): Promise<boolean> {
  try {
    await axios.post<LoginResponse>(`https://localhost:7121/Authentication/validate-email`, validateEmailDto);
    return true;
  } catch (error) {
    return false;
  }
}

export async function Register(registerDto: RegisterDto): Promise<string[] | null> {
  try {
    registerDto.FrontendBaseUrl = `${window.location.origin}/email-confirmation`;
    await axios.post(`https://localhost:7121/Authentication/Register`, registerDto);
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
    await axios.post(`https://localhost:7121/Authentication/reset-password`, resetPasswordDto);
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
  forgotPasswordDto.FrontedBaseUrl = `${window.location.origin}/reset-password`;
  try {
    await axios.post(`https://localhost:7121/Authentication/forgot-password`, forgotPasswordDto);
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
  localStorage.removeItem("access_token");
  window.location.href = "/login";
};
