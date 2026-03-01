import TransactionForm from "../../Forms/Transaction/TransactionForm";
import type { Transaction } from "../../Models/Transaction";
import Box from "../Box/Box";

interface Props {
  onAddTransaction: (transaction: Transaction) => void;
}

export const AddTransaction = ({ onAddTransaction }: Props) => {
  return (
    <Box header="Add Transaction">
      <TransactionForm onAddTransaction={onAddTransaction}>
        <button
          type="submit"
          className="py-2 px-5 bg-blue-600 text-white rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
          Add
        </button>
      </TransactionForm>
    </Box>
  );
};
