import axios from "axios";
import type { LoginDTO } from "../../DTOs/LoginDTO";
import type { LoginResponse } from "../../Models/LoginResponse";

export async function Authenticate(loginDTO: LoginDTO): Promise<boolean> {
        const response = await axios.post<LoginResponse>(`https://localhost:7121/Authentication`, loginDTO);
        localStorage.setItem("access_token", response.data.access_token);
        return true;
}

export const isAuthenticated = (): boolean => {
  const token = localStorage.getItem("access_token");
  return !!token;
};

export const logout = (): void => {
  localStorage.removeItem("access_token");
  window.location.href = "/login";
};
