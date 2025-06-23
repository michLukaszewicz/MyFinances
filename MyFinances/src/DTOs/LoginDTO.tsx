import { useForm } from "react-hook-form";

export type LoginDto = {
    Email: string;
    Password: string;
}

const { register, handleSubmit, formState: { errors } } = useForm<LoginDto>();