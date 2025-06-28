import { useForm } from "react-hook-form";
import { Link } from "react-router-dom";
import type { ResetPasswordDto } from "../Dtos/ResetPasswordDto";

const ResetPasswordForm = () => {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ResetPasswordDto>({ mode: "onSubmit" });

  const onSubmit = async (data: ResetPasswordDto) => {
    var success = await ResetPassword(data);
    if (success) {
      window.location.href = "/login";
    } else {
      alert("Reset failed. Please check the form");
    }
  };

  return (
    <form className="p-5" onSubmit={handleSubmit(onSubmit)}>
      <div className="flex flex-col gap-2 mb-3">
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
      <Link to={"/login"}>
        <button className="w-full mb-4 py-2 px-4 bg-gray-300 text-white rounded-md hover:bg-blue-600 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
          Login
        </button>
      </Link>
      <Link to={"/register"}>
        <button className="w-full py-2 px-4 bg-gray-300 text-white rounded-md hover:bg-blue-600 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
          Register
        </button>
      </Link>
    </form>
  );
};

export default ResetPasswordForm;
