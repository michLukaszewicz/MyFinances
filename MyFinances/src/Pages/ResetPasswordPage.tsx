import Box from "../Components/Box/Box";
import PageContent from "../Components/PageContent/PageContent";
import ResetPasswordForm from "../Forms/ResetPasswordForm";

const ResetPasswordPage = () => {
  return (
    <PageContent>
      <div className="sm:w-xl sm:mx-auto">
        <Box header="Set New Password">
          <p className="text-gray-500 text-sm mb-3">Please enter your new password</p>
          <ResetPasswordForm />
        </Box>
      </div>
    </PageContent>
  );
};

export default ResetPasswordPage;
