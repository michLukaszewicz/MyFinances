import axios from 'axios';
import { ROUTES } from '../../Routes/RoutesConsts';

let navigate: ((path: string) => void) | null = null;

export const setNavigate = (navFunction: (path: string) => void) => {
    navigate = navFunction;
};

axios.interceptors.response.use(
    (response) => response,
    (error) => {
        if (error.response && error.response.status === 401){
            if (navigate) {
                navigate(ROUTES.auth.login);
            }
        }
        return Promise.reject(error);
    }
)

export default axios;