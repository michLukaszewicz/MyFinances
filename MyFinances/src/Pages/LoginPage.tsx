import Box from "../Components/Box/Box";
import PageContent from "../Components/PageContent/PageContent";
import LoginForm from "../Components/Forms/LoginForm";

const LoginPage = () => {
  return (
    <PageContent>
      <div className="sm:w-xl sm:mx-auto">
        <Box header="Welcome Back">
          <p className="text-gray-500 text-sm mb-3">Please enter your details</p>
          <LoginForm />
        </Box>
      </div>
    </PageContent>
  );
};

export default LoginPage;
