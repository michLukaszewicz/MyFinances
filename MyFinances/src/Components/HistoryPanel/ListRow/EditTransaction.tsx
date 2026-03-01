import TransactionForm from "../../../Forms/Transaction/TransactionForm";
import type { Transaction } from "../../../Models/Transaction";

interface Props {
  transaction: Transaction;
  onEditTransaction: (transaction: Transaction) => void;
  onEditCancel: (id: number) => void;
}

export const EditTransaction = ({ transaction, onEditTransaction, onEditCancel }: Props) => (
  <TransactionForm onSubmitAction={onEditTransaction} basedOnTransaction={transaction}>
    <button
      type="submit"
      className="mb-1 py-1 w-20 bg-blue-600 text-white rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
      Edit
    </button>
    <button
      type="button"
      className="py-1 w-20 bg-red-500 text-white rounded-md hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2"
      onClick={() => onEditCancel(transaction.id)}>
      Cancel
    </button>
  </TransactionForm>
);
