import Box from "../../Components/Box/Box";
import PageContent from "../../Components/PageContent/PageContent";
import ForgotPasswordForm from "../../Forms/ForgotPasswordForm";

const ForgotPasswordPage = () => {
  return (
    <PageContent>
      <div className="sm:w-xl sm:mx-auto">
        <Box header="Forgot Password">
          <p className="text-gray-500 text-sm">Please provide the email address associated with Your account to recover the password</p>
          <ForgotPasswordForm />
        </Box>
      </div>
    </PageContent>
  );
};

export default ForgotPasswordPage;
