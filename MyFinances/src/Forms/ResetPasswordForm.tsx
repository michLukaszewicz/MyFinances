import { useForm } from "react-hook-form";
import { Link, useSearchParams } from "react-router-dom";
import type { ResetPasswordDto } from "../Models/Dtos/ResetPasswordDto";
import { ResetPassword } from "../Services/ApiServices/AuthenticationService";
import { ROUTES } from "../Routes/RoutesConsts";

const ResetPasswordForm = () => {
  const [searchParams] = useSearchParams();

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<ResetPasswordDto>({ mode: "onSubmit" });

  const onSubmit = async (data: ResetPasswordDto) => {
    data.Token = searchParams.get("token") || "";
    data.userId = searchParams.get("userId") || "";
    var results = await ResetPassword(data);
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
    <form className="p-5" onSubmit={handleSubmit(onSubmit)} noValidate>
      <div className="flex flex-col gap-2 mb-3">
        {errors.root?.message && <div className="text-red-500 text-sm mb-2 whitespace-pre-line">{errors.root.message}</div>}
        <label htmlFor="new-password" className="block text-xl font-medium text-gray-700">
          New Password
        </label>
        <input
          autoFocus
          type="password"
          id="new-password"
          {...register("NewPassword", { required: "New Password is required" })}
          className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"
          placeholder="********"></input>
        {errors.NewPassword && <span className="text-red-500 text-sm">{errors.NewPassword.message}</span>}
      </div>
      <div className="mb-7 flex flex-col gap-2">
        <label htmlFor="confirm-password" className="block text-xl font-medium text-gray-700">
          Confirm Password
        </label>
        <input
          type="password"
          id="confirm-password"
          {...register("ConfirmPassword", { required: "Confirm Password is required" })}
          className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"
          placeholder="**********"></input>
        {errors.ConfirmPassword && <span className="text-red-500 text-sm">{errors.ConfirmPassword.message}</span>}
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

export default ResetPasswordForm;
