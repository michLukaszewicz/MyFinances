import { useForm } from "react-hook-form";
import { Link } from "react-router-dom";
import { ForgotPassword } from "../Services/ApiServices/AuthenticationService";
import type { ForgotPasswordDto } from "../Models/Dtos/ForgotPasswordDto";
import { ROUTES } from "../Routes/RoutesConsts";

const ForgotPasswordForm = () => {
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<ForgotPasswordDto>({ mode: "onSubmit" });

  const onSubmit = async (data: ForgotPasswordDto) => {
    var results = await ForgotPassword(data);
    if (results === null) {
      alert("If an account with that email exists, you will receive a password reset link shortly.");
    } else {
      setError("root", {
        type: "server",
        message: results.join("\n"),
      });
    }
  };

  return (
    <form className="p-5" onSubmit={handleSubmit(onSubmit)} noValidate>
      <div className="flex flex-col gap-2 mb-3">
        {errors.root?.message && <div className="text-red-500 text-sm mb-2 whitespace-pre-line">{errors.root.message}</div>}
        <label htmlFor="email" className="block text-xl font-medium text-gray-700">
          Email address
        </label>
        <input
          autoFocus
          type="email"
          id="email"
          {...register("Email", { required: "Email is required" })}
          className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"
          placeholder="you@example.com"></input>
        {errors.Email && <span className="text-red-500 text-sm">{errors.Email.message}</span>}
      </div>
      <div className="flex items-center justify-between">
        <button
          type="submit"
          className="w-full py-2 px-4 bg-blue-600 text-white rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
          Reset Password
        </button>
      </div>
      <hr className="my-6 text-gray-200 font-bold" />
      <Link to={ROUTES.auth.login}>
        <button className="w-full mb-4 py-2 px-4 bg-gray-300 text-white rounded-md hover:bg-blue-600 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
          Login
        </button>
      </Link>
      <Link to={ROUTES.auth.register}>
        <button className="w-full py-2 px-4 bg-gray-300 text-white rounded-md hover:bg-blue-600 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
          Register
        </button>
      </Link>
    </form>
  );
};

export default ForgotPasswordForm;
