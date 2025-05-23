import { useForm } from "react-hook-form";

type LoginDTO = {
    userName: string;
    password: string;
}

const { register, handleSubmit, formState: { errors } } = useForm<LoginDTO>();