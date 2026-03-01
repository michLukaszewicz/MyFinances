import type { Transaction } from "../../Models/Transaction";
import type { ClientTransaction } from "../../Models/Dtos/TransactionClientDto";
import TransactionHistory from "../Transactions/TransactionsHistory";
import { AddTransaction } from "../Transactions/AddTransaction";

interface Props {
  transactionHistory: ClientTransaction[];
  onAddTransaction: (transaction: Transaction) => void;
  onDeleteTransaction: (id: number) => void;
  onEditTransaction: (transaction: Transaction) => void;
}

const TransactionsPanel = ({ transactionHistory, onAddTransaction, onDeleteTransaction, onEditTransaction }: Props) => {
  return (
    <>
      <AddTransaction onAddTransaction={onAddTransaction} />
      <TransactionHistory transactionHistory={transactionHistory} onDeleteTransaction={onDeleteTransaction} onEditTransaction={onEditTransaction} />
    </>
  );
};

export default TransactionsPanel;
