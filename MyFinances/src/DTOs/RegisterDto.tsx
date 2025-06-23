import { useForm } from "react-hook-form";

export type RegisterDto = {
    FirstName: string;
    Email: string;
    Password: string;
    ConfirmPassword: string;
}

const { register, handleSubmit, formState: { errors } } = useForm<RegisterDto>();