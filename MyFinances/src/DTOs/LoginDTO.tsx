import { useForm } from "react-hook-form";

export type LoginDTO = {
    username: string;
    password: string;
}

const { register, handleSubmit, formState: { errors } } = useForm<LoginDTO>();