import type { Transaction } from "../../Models/Transaction";
import Box from "../Box/Box";

type Props = {
    history: Transaction[];
}

const BalanceChart = ({history}: Props) => {
  return (
    <Box>
        <h1>Balance Chart</h1>
    </Box>
  )
}

export default BalanceChart