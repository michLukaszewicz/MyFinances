import { useLocation } from "react-router-dom";
import Box from "../../Components/Box/Box";
import PageContent from "../../Components/PageContent/PageContent";
import LoginForm from "../../Forms/LoginForm";

const ExternalLoginPage = () => {
const {search} = useLocation();
const params = new URLSearchParams(search);

const providerName = params.get("providerName") || "";
const providerKey = params.get("providerKey") || "";

  return (
    <PageContent>
      <div className="sm:w-xl sm:mx-auto">
        <Box header="Welcome Back">
          <p className="text-gray-500 text-sm mb-3">
            Your account is not linked to the external provider {providerName}.<br />
            Please enter your details to log in and link your account with this provider. <br />
            This will allow you to log in with it in the future.
          </p>
          <LoginForm providerName={providerName} providerKey={providerKey} />
        </Box>
      </div>
    </PageContent>
  );
};

export default ExternalLoginPage;
