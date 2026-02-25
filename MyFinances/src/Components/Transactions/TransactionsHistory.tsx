import type { ClientTransaction } from "../../Models/Dtos/TransactionClientDto";
import Box from "../Box/Box";
import ListRow from "../HistoryPanel/ListRow/ListRow";

interface Props {
    transactionHistory: ClientTransaction[];
}

const TransactionHistory = ({transactionHistory} : Props) => 
  <Box header="History Panel">
    <div className="flex justify-between mb-3">
      <a href="#" className="text-gray-500">
        View All &gt;
      </a>
    </div>
    <div className="flex flex-col justify-between">
      {transactionHistory.map((transaction) => (
        <ListRow key={transaction.clientId} transaction={transaction} />
      ))}
    </div>
  </Box>;

export default TransactionHistory;
