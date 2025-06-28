import { Link } from "react-router-dom";
import Box from "../Components/Box/Box";
import PageContent from "../Components/PageContent/PageContent";
import RegisterForm from "../Forms/RegisterForm";

const RegisterPage = () => {
  return (
    <PageContent>
      <div className="sm:w-xl sm:mx-auto">
        <Box header="Register">
          <p className="text-gray-500 text-sm mb-3">Nice to see You! Tell us about Yourself</p>
          <div className="p-5">
          <RegisterForm />
          <hr className="my-3 text-gray-200 font-bold" />
          <div className="flex items-center">
            <p className="py-2 ">Have an account?</p>
            <Link to={"/login"}>
              <span className="text-blue-600 hover:text-blue-800 ml-1">Login Here</span>
            </Link>
            <Link to={"/forgot-password"} className="ml-auto text-blue-600 hover:text-blue-800">
              Forgot Password?
            </Link>
          </div>
          </div>
        </Box>
      </div>
    </PageContent>
  );
};

export default RegisterPage;
