import axios from 'axios';
import { useNavigate } from 'react-router-dom';
import { ROUTES } from '../../Routes/RoutesConsts';

const navigate = useNavigate();
axios.interceptors.response.use(
    (response) => response,
    (error) => {
        if (error.response && error.response.status === 401){
            navigate(ROUTES.auth.login);
        }
        return Promise.reject(error);
    }
)

export default axios;