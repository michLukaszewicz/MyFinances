import { Link } from "react-router-dom";
import Box from "../Components/Box/Box";
import PageContent from "../Components/PageContent/PageContent";

type Props = {};

const LoginPage = (props: Props) => {
  return (
    <PageContent>
      <div className="sm:w-xl sm:mx-auto">
        <Box header="Welcome Back">
          <p className="text-gray-500 text-sm mb-3">Please enter your details</p>
          <form className="p-5">
            <div>
              <div className="flex flex-col gap-2 mb-3">
                <label htmlFor="email" className="block text-xl font-medium text-gray-700">
                  Email
                </label>
                <input
                  autoFocus
                  type="email"
                  id="email"
                  className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"
                  placeholder="email@exeple.com"></input>
              </div>
              <div className="mb-2 flex flex-col gap-2">
                <label htmlFor="password" className="block text-xl font-medium text-gray-700">
                  Password
                </label>
                <input
                  type="password"
                  id="password"
                  className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"
                  placeholder="**********"></input>
              </div>
              <div className="flex items-center mb-5">
                <input type="checkbox" id="RememberMe" className="mr-3 w-4.5 h-4.5" />
                <label htmlFor="RememberMe">Remember Me</label>
                <Link to={"/forgot-password"} className="ml-auto text-blue-600 hover:text-blue-800">
                  Forgot Password?{" "}
                </Link>
              </div>
              <div className="flex items-center justify-between">
                <button
                  type="submit"
                  className="w-full py-2 px-4 bg-blue-600 text-white rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
                  Login
                </button>
              </div>
              <hr className="my-6 text-gray-200 font-bold"/>
              <Link to={"/register"} >
                <button className="w-full py-2 px-4 bg-gray-300 text-white rounded-md hover:bg-blue-600 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
                  Register
                </button>
              </Link>
            </div>
          </form>
        </Box>
      </div>
    </PageContent>
  );
};

export default LoginPage;
