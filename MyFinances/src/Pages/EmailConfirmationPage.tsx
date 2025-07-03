import Box from "../Components/Box/Box";
import { useSearchParams } from "react-router-dom";
import PageContent from "../Components/PageContent/PageContent";
import { ValidateEmail } from "../Services/ApiServices/AuthenticationService";
import { useEffect, useRef, useState } from "react";
import type { ValidateEmailDto } from "../Models/Dtos/ValidateEmailDto";

const EmailConfirmationPage = () => {
const [searchParams] = useSearchParams();
const [result, setResult] = useState<boolean>(false);
const userId: string = searchParams.get("userId") || "";
const token: string = searchParams.get("token") || "";

const alreadyCalledRef = useRef<boolean>(false);

  useEffect(() => {
    if (!userId || !token || alreadyCalledRef.current) return;
    alreadyCalledRef.current = true;

    const validateEmail = async () => {
      const validateEmailDto: ValidateEmailDto = {
        UserId: userId,
        Token: token,
      };

      try {
        const response = await ValidateEmail(validateEmailDto);
        setResult(response);
      } catch (error) {
        console.error("Validation failed", error);
        setResult(false);
      }
    };

    validateEmail();
  }, [userId, token]);

  return (
    <PageContent>
      <div className="sm:w-xl sm:mx-auto">
        <Box header="Validate Email">
          <div className="p-5">
            {result ? (
              <div className="text-green-500 text-lg">
                Your email has been successfully confirmed!
              </div>
            ) : (
              <div className="text-red-500 text-lg">
                There was an error confirming your email. Please try again.
              </div>
            )}
          </div>
        </Box>
      </div>
    </PageContent>
  );
};

export default EmailConfirmationPage;
