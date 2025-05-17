import Box from "../Components/Box/Box";
import PageContent from "../Components/PageContent/PageContent";

type Props = {};

const LoginPage = (props: Props) => {
  return (
    <PageContent>
      <Box header="Welcome Back">
        <p>Please enter your details</p>
      </Box>
    </PageContent>
  );
};

export default LoginPage;
