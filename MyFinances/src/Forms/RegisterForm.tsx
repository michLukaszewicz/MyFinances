import { useForm } from "react-hook-form";
import { Register } from "../Services/ApiServices/AuthenticationService";
import type { RegisterDto } from "../Models/Dtos/RegisterDto";

type RegisterFormProps = {
  email: string | null;
  name: string | null;
  provider: string | null;
}

const RegisterForm = ({ email, name, provider }: RegisterFormProps) => {
  const {
    register,
    handleSubmit,
    setError,
    watch,
    formState: { errors },
  } = useForm<RegisterDto>({ 
    mode: "onSubmit", 
  defaultValues: {
    Email: email ?? "",
    Name: name ?? "",
    Provider: provider ?? ""
  }});

  const password = watch("Password");

  const onSubmit = async (data: RegisterDto) => {
    var results = await Register(data);
    if (results === null) {
      window.location.href = "/login";
    } else {
      setError("root", {
        type: "server",
        message: results.join("\n"),
      });
    }
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate>
      <div className="flex flex-col gap-2 mb-3">
        {errors.root?.message && <div className="text-red-500 text-sm mb-2 whitespace-pre-line">{errors.root.message}</div>}
        <label htmlFor="name" className="block text-xl font-medium text-gray-700">
          Name
        </label>
        <input
          autoFocus
          type="text"
          id="Name"
          {...register("Name", { required: "Name is required" })}
          className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"
          placeholder="Name"></input>
        {errors.Name && <span className="text-red-500 text-sm">{errors.Name.message}</span>}
      </div>
      <div className="flex flex-col gap-2 mb-3">
        <label htmlFor="email" className="block text-xl font-medium text-gray-700">
          Email
        </label>
        <input
          type="email"
          id="Email"
          {...register("Email", { required: "Email is required" })}
          className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"
          placeholder="email@exeple.com"></input>
        {errors.Email && <span className="text-red-500 text-sm">{errors.Email.message}</span>}
      </div>
      <div className="mb-2 flex flex-col gap-2">
        <label htmlFor="password" className="block text-xl font-medium text-gray-700">
          Password
        </label>
        <input
          type="password"
          id="Password"
          {...register("Password", {
            required: "Password is required",
            minLength: { value: 8, message: "Password must be at least 8 characters long" },
          })}
          className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"
          placeholder="**********"></input>
        {errors.Password && <span className="text-red-500 text-sm">{errors.Password.message}</span>}
      </div>
      <div className="mb-2 flex flex-col gap-2">
        <label htmlFor="confirmPassword" className="block text-xl font-medium text-gray-700">
          Confirm Password
        </label>
        <input
          type="password"
          id="ConfirmPassword"
          {...register("ConfirmPassword", {
            required: "Confirm Password is required",
            validate: (value) => value === password || "Passwords do not match",
          })}
          className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"
          placeholder="**********"></input>
        {errors.ConfirmPassword && <span className="text-red-500 text-sm">{errors.ConfirmPassword.message}</span>}
      </div>
      <p className="text-gray-500 text-sm text-center mb-2"> Registration confirmation will be emailed to You</p>
      <div className="flex items-center justify-between">
        <button
          type="submit"
          className="w-full py-2 px-4 my-4 bg-blue-600 text-white rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
          Register
        </button>
      </div>

      <input type="hidden" id="Provider" value={provider ?? ""} {...register("FrontendBaseUrl")} />
    </form>
  );
};

export default RegisterForm;
