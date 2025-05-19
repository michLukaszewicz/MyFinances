import { Link } from "react-router-dom";
import Box from "../Components/Box/Box";
import PageContent from "../Components/PageContent/PageContent";

const RegisterPage = () => {
  return (
    <PageContent>
      <div className="sm:w-xl sm:mx-auto">
        <Box header="Register">
          <p className="text-gray-500 text-sm mb-3">Nice to see You! Tell us about Yourself</p>
          <form className="p-5">
            <div>
              <div className="flex flex-col gap-2 mb-3">
                <label htmlFor="username" className="block text-xl font-medium text-gray-700">
                  Username
                </label>
                <input
                  autoFocus
                  type="text"
                  id="username"
                  className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"
                  placeholder="username"></input>
              </div>
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
                  className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"
                  placeholder="**********"></input>
              </div>
              <div className="mb-2 flex flex-col gap-2">
                <label htmlFor="password" className="block text-xl font-medium text-gray-700">
                  Confirm Password
                </label>
                <input
                  type="password"
                  className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"
                  placeholder="**********"></input>
              </div>
              <p className="text-gray-500 text-sm text-center mb-2"> Registration confirmation will be emailed to You</p>
              <div className="flex items-center justify-between">
                <button
                  type="submit"
                  className="w-full py-2 px-4 my-4 bg-blue-600 text-white rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
                  Register
                </button>
              </div>
              <hr className="my-3 text-gray-200 font-bold" />
              <div className="flex items-center">
                <p className="py-2 ">Have and account?</p>
                <Link to={"/login"}>
                  <span className="text-blue-600 hover:text-blue-800 ml-1">Login Here</span>
                </Link>
                <Link to={"/forgot-password"} className="ml-auto text-blue-600 hover:text-blue-800">
                  Forgot Password?
                </Link>
              </div>
            </div>
          </form>
        </Box>
      </div>
    </PageContent>
  );
};

export default RegisterPage;
