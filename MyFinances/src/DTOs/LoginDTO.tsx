import { useForm } from "react-hook-form";

export type LoginDTO = {
    UserName: string;
    Password: string;
}

const { register, handleSubmit, formState: { errors } } = useForm<LoginDTO>();