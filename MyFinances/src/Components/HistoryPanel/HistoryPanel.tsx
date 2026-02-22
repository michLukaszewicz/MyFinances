import type { Transaction } from "../../Models/Transaction";
import Box from "../Box/Box";
import ListRow from "./ListRow/ListRow";
import AddTransaction from "../Transactions/AddTransaction";
import type { ClientTransaction } from "../../Models/Dtos/TransactionClientDto";

interface Props {
  history: ClientTransaction[];
  onAddTransaction: (transaction: Transaction) => void;
}

const HistoryPanel = ({ history, onAddTransaction }: Props) => {
  return (
    <>
      <AddTransaction onAddTransaction={onAddTransaction} />
      <Box header="History Panel">
        <div className="flex justify-between mb-3">
          <a href="#" className="text-gray-500">
            View All &gt;
          </a>
        </div>
        <div className="flex flex-col justify-between">
          {history.map((transaction) => (
            <ListRow key={transaction.clientId} transaction={transaction} />
          ))}
        </div>
      </Box>
    </>
  );
};

export default HistoryPanel;
