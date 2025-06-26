import axios from "axios";
import type { LoginDto } from "../../Dtos/LoginDto";
import type { RegisterDto } from "../../Dtos/RegisterDto";
import type { LoginResponse } from "../../Models/LoginResponse";

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

export async function Register(registerDto: RegisterDto): Promise<string[] | null> {
  try {
    await axios.post(`https://localhost:7121/Authentication/Register`, registerDto);
    return null;
  } catch (error: any) {
    if (axios.isAxiosError(error) && error.response) {
      if (Array.isArray(error.response.data.Errors)) {
        return error.response.data.Errors;
      }
      if (Array.isArray(error.response.data.errors)) {
        return error.response.data.errors;
      }
    }
    return ["Registration failed. Please try again."];
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
