import axios from "axios";
import type { LoginDTO } from "../../DTOs/LoginDTO";
import type { LoginResponse } from "../../Models/LoginResponse";

export async function Authenticate(loginDTO: LoginDTO): Promise<boolean> {
        const response = await axios.post<LoginResponse>(`https://localhost:7121/Authentication`, loginDTO);
        localStorage.setItem("access_token", response.data.access_token);
        return true;
}